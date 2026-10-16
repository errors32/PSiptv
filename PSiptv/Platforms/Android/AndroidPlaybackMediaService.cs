using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using PSiptv.Services;
using PSiptv.Views;

namespace PSiptv;

// Publish the existing VLC player to Android/HyperOS; never create a second player.
#pragma warning disable CS8602, CS8603, CS8604
[Service(Name = "com.PS.PSiptv.PlaybackMediaService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
public sealed class AndroidPlaybackMediaService : Service
{
    private const string ChannelId = "psiptv.playback";
    private const int NotificationId = 0x5058;
    private const string PlayAction = "psiptv.media.PLAY";
    private const string PauseAction = "psiptv.media.PAUSE";
    private const string StopAction = "psiptv.media.STOP";
    private const string RewindAction = "psiptv.media.REWIND";
    private const string ForwardAction = "psiptv.media.FORWARD";
    private static PlaybackView? owner;
    private static AndroidPlaybackMediaService? running;
    private MediaSession? session;
    private Handler? handler;
    private PlaybackView? publishedOwner;
    private PSiptv.Core.MediaItem? publishedItem;
    private PlaybackStateCode? publishedState;
    private bool publishedSeekable;
    private long publishedDuration = -1;
    private Bitmap? artwork;
    private CancellationTokenSource? artworkCancellation;

    internal static void Attach(PlaybackView playback)
    {
        owner = playback;
        var context = Android.App.Application.Context;
        try { context.StartForegroundService(new Intent(context, typeof(AndroidPlaybackMediaService))); }
        catch (Java.Lang.Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    internal static void Detach(PlaybackView playback)
    {
        if (owner != playback) return;
        owner = null;
        // Remove the old session synchronously, before another view can take ownership.
        running?.ClearSession();
        Android.App.Application.Context.StopService(new Intent(Android.App.Application.Context,
            typeof(AndroidPlaybackMediaService)));
    }

    public override void OnCreate()
    {
        base.OnCreate();
        running = this;
        handler = new Handler(Looper.MainLooper!);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId,
            "Reprodução multimédia", NotificationImportance.Low));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (owner?.ActiveMedia is null)
        {
            // A queued foreground start still has to post its notification before stopping.
            StartForeground(NotificationId, new Notification.Builder(this, ChannelId)
                .SetSmallIcon(Resource.Drawable.ic_auto_attribution).SetContentTitle("PSiptv").Build());
            ClearSession();
            StopSelf(startId);
            return StartCommandResult.NotSticky;
        }
        EnsureSession();
        Publish(true);
        handler?.RemoveCallbacks(Tick);
        handler?.PostDelayed(Tick, 1000);
        if (intent?.Action is { } action) Execute(action);
        // VLC cannot be restored from a service intent after process death.
        return StartCommandResult.NotSticky;
    }

    private void EnsureSession()
    {
        if (session is not null) return;
        session = new MediaSession(this, "PSiptv.Playback");
        session.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
        session.SetCallback(new SessionCallback(this), handler);
        session.SetSessionActivity(LaunchIntent());
        session.Active = true;
    }

    private void Tick()
    {
        if (owner?.ActiveMedia is null) return;
        Publish(false);
        handler?.PostDelayed(Tick, 1000);
    }

    private void Publish(bool foreground)
    {
        if (owner is not { ActiveMedia: { } item } playback || session is null) return;
        var changedItem = publishedOwner != playback || publishedItem != item;
        if (changedItem)
        {
            artworkCancellation?.Cancel();
            artworkCancellation?.Dispose();
            artworkCancellation = new CancellationTokenSource();
            artwork?.Dispose();
            artwork = AppArtwork();
            publishedOwner = playback;
            publishedItem = item;
            publishedDuration = -1;
            _ = LoadArtworkAsync(item, playback, artworkCancellation.Token);
        }
        var duration = Math.Max(0, (long)(playback.Duration * 1000));
        if (changedItem || duration != publishedDuration)
        {
            publishedDuration = duration;
            PublishMetadata();
        }
        var state = playback.SystemPlaybackState;
        var seekable = playback.CanSeek && item.Kind != PSiptv.Core.MediaKind.Channel;
        var actions = PlaybackState.ActionPlay | PlaybackState.ActionPause |
            PlaybackState.ActionPlayPause | PlaybackState.ActionStop;
        if (seekable) actions |= PlaybackState.ActionSeekTo | PlaybackState.ActionRewind | PlaybackState.ActionFastForward;
        using var stateBuilder = new PlaybackState.Builder();
        using var nativeState = stateBuilder.SetActions(actions)
            .SetState(state, Math.Max(0, (long)(playback.Position * 1000)),
                state == PlaybackStateCode.Playing ? playback.SystemPlaybackSpeed : 0,
                SystemClock.ElapsedRealtime()).Build();
        session.SetPlaybackState(nativeState);
        if (foreground || changedItem || state != publishedState || seekable != publishedSeekable)
        {
            publishedState = state;
            publishedSeekable = seekable;
            var notification = BuildNotification();
            if (foreground) StartForeground(NotificationId, notification);
            else ((NotificationManager?)GetSystemService(NotificationService))?.Notify(NotificationId, notification);
            RefreshPictureInPicture(playback);
        }
    }

    private void PublishMetadata()
    {
        if (publishedItem is not { } item) return;
        using var builder = new MediaMetadata.Builder();
        builder.PutString(MediaMetadata.MetadataKeyMediaId, item.Id)
            .PutString(MediaMetadata.MetadataKeyTitle, item.Name)
            .PutString(MediaMetadata.MetadataKeyDisplayTitle, item.Name)
            .PutString(MediaMetadata.MetadataKeyArtist, item.Category)
            .PutString(MediaMetadata.MetadataKeyDisplaySubtitle, item.Category)
            .PutLong(MediaMetadata.MetadataKeyDuration, publishedDuration);
        if (artwork is not null)
            builder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, artwork)
                .PutBitmap(MediaMetadata.MetadataKeyDisplayIcon, artwork);
        using var metadata = builder.Build();
        session?.SetMetadata(metadata);
    }

    private async Task LoadArtworkAsync(PSiptv.Core.MediaItem item, PlaybackView playback, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(item.Logo)) return;
        try
        {
            var path = await LogoCacheService.LoadAsync(item.Logo, token);
            if (path is null || token.IsCancellationRequested) return;
            using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
            await BitmapFactory.DecodeFileAsync(path, bounds);
            var sample = 1;
            while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > 512) sample *= 2;
            using var options = new BitmapFactory.Options { InSampleSize = sample };
            var bitmap = await BitmapFactory.DecodeFileAsync(path, options);
            if (bitmap is null) return;
            if (token.IsCancellationRequested || owner != playback || publishedItem != item || session is null)
            {
                bitmap?.Dispose();
                return;
            }
            artwork?.Dispose();
            artwork = bitmap;
            PublishMetadata();
            ((NotificationManager?)GetSystemService(NotificationService))?.Notify(NotificationId, BuildNotification());
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private PendingIntent? LaunchIntent()
    {
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        return launch is null ? null : PendingIntent.GetActivity(this, 0, launch,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    private static PendingIntent CommandIntent(Context context, string action) =>
        PendingIntent.GetService(context, 0,
            new Intent(context, typeof(AndroidPlaybackMediaService)).SetAction(action),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

    private Bitmap? AppArtwork()
    {
        using var drawable = PackageManager?.GetApplicationIcon(PackageName!);
        if (drawable is null) return null;
        var bitmap = Bitmap.CreateBitmap(256, 256, Bitmap.Config.Argb8888!);
        using var canvas = new Canvas(bitmap);
        drawable.SetBounds(0, 0, 256, 256);
        drawable.Draw(canvas);
        return bitmap;
    }

    private Notification.Action NotificationAction(int icon, string title, string action)
    {
        using var nativeIcon = Android.Graphics.Drawables.Icon.CreateWithResource(this, icon);
        using var builder = new Notification.Action.Builder(nativeIcon, title, CommandIntent(this, action));
        return builder.Build();
    }

    private Notification BuildNotification()
    {
        var playing = publishedState is PlaybackStateCode.Playing or PlaybackStateCode.Buffering or PlaybackStateCode.Connecting;
        using var style = new Notification.MediaStyle();
        style.SetMediaSession(session?.SessionToken);
        using var builder = new Notification.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_auto_attribution)
            .SetContentTitle(publishedItem?.Name ?? "PSiptv")
            .SetContentText(publishedItem?.Category ?? "")
            .SetContentIntent(LaunchIntent()).SetOnlyAlertOnce(true)
            .SetCategory(Notification.CategoryTransport).SetVisibility(NotificationVisibility.Public)
            .SetOngoing(playing).SetShowWhen(false);
        if (artwork is not null) builder.SetLargeIcon(artwork);
        if (publishedSeekable)
            builder.AddAction(NotificationAction(Android.Resource.Drawable.IcMediaRew, "Recuar 15 segundos", RewindAction));
        builder.AddAction(NotificationAction(playing ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay,
            playing ? "Pausar" : "Reproduzir", playing ? PauseAction : PlayAction));
        if (publishedSeekable)
            builder.AddAction(NotificationAction(Android.Resource.Drawable.IcMediaFf, "Avançar 30 segundos", ForwardAction));
        builder.AddAction(NotificationAction(Android.Resource.Drawable.IcMenuCloseClearCancel, "Parar", StopAction));
        style.SetShowActionsInCompactView(publishedSeekable ? [0, 1, 2] : [0, 1]);
        return builder.SetStyle(style).Build();
    }

    internal static void ConfigurePictureInPicture(PictureInPictureParams.Builder builder, PlaybackView playback)
    {
        var playing = playback.IsPlaying;
        var context = Android.App.Application.Context;
        using var icon = Android.Graphics.Drawables.Icon.CreateWithResource(context,
            playing ? Android.Resource.Drawable.IcMediaPause : Android.Resource.Drawable.IcMediaPlay);
        var title = playing ? "Pausar" : "Reproduzir";
        using var action = new RemoteAction(icon, title, title,
            CommandIntent(context, playing ? PauseAction : PlayAction));
        builder.SetActions(new[] { action });
    }

    private static void RefreshPictureInPicture(PlaybackView playback)
    {
        if (!PictureInPictureService.IsActive || PictureInPictureService.Current != playback) return;
        using var builder = new PictureInPictureParams.Builder();
        ConfigurePictureInPicture(builder, playback);
        try { Platform.CurrentActivity?.SetPictureInPictureParams(builder.Build()!); }
        catch (Java.Lang.Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private async void Execute(string action, long position = 0)
    {
        if (owner is not { } playback) return;
        try
        {
            switch (action)
            {
                case PlayAction:
                    if (!playback.IsPlaying) await playback.TogglePlayPauseAsync();
                    break;
                case PauseAction:
                    playback.Pause();
                    break;
                case StopAction:
                    await playback.StopForExternalPlaybackAsync();
                    break;
                case RewindAction:
                    if (publishedSeekable) await playback.SeekByAsync(-15);
                    break;
                case ForwardAction:
                    if (publishedSeekable) await playback.SeekByAsync(30);
                    break;
                case "seek":
                    await playback.SeekFromSystemAsync(position);
                    break;
            }
            Publish(false);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private void ClearSession()
    {
        handler?.RemoveCallbacks(Tick);
        artworkCancellation?.Cancel();
        artworkCancellation?.Dispose();
        artworkCancellation = null;
        if (session is not null)
        {
            session.Active = false;
            session.Release();
            session.Dispose();
            session = null;
        }
        artwork?.Dispose();
        artwork = null;
        publishedOwner = null;
        publishedItem = null;
        publishedState = null;
        publishedDuration = -1;
        StopForeground(StopForegroundFlags.Remove);
    }

    public override void OnDestroy()
    {
        ClearSession();
        if (running == this) running = null;
        handler?.Dispose();
        handler = null;
        base.OnDestroy();
    }

    private sealed class SessionCallback(AndroidPlaybackMediaService service) : MediaSession.Callback
    {
        public override void OnPlay() => service.Execute(PlayAction);
        public override void OnPause() => service.Execute(PauseAction);
        public override void OnStop() => service.Execute(StopAction);
        public override void OnSeekTo(long pos) => service.Execute("seek", pos);
        public override void OnRewind() => service.Execute(RewindAction);
        public override void OnFastForward() => service.Execute(ForwardAction);
    }
}
#pragma warning restore CS8602, CS8603, CS8604
