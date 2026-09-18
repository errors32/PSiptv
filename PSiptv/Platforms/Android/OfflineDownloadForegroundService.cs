using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace PSiptv.Services;

[Service(Name = "com.PS.PSiptv.OfflineDownloadForegroundService", Enabled = true,
    Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class OfflineDownloadForegroundService : Service
{
    private const string ProgressChannelId = "offline-downloads-progress";
    private const string CompletedChannelId = "offline-downloads-completed";
    private const int ProgressNotificationId = 48230;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForeground(ProgressNotificationId, BuildProgressNotification());
        var id = intent?.GetStringExtra("id") ?? "";
        _ = Task.Run(async () =>
        {
            PowerManager.WakeLock? wakeLock = null;
            try
            {
                wakeLock = ((PowerManager?)GetSystemService(PowerService))?
                    .NewWakeLock(WakeLockFlags.Partial, "PSiptv:OfflineDownload");
                wakeLock?.Acquire();
                if (id.Length > 0) await OfflineDownloadService.RunInBackgroundAsync(id);
                if (await OfflineDownloadService.FindByIdAsync(id) is { State: PSiptv.Core.OfflineDownloadState.Completed
                    or PSiptv.Core.OfflineDownloadState.Failed } result)
                    ShowResultNotification(result);
            }
            finally
            {
                if (wakeLock?.IsHeld == true) wakeLock.Release();
                wakeLock?.Dispose();
                StopSelfResult(startId);
            }
        });
        return StartCommandResult.RedeliverIntent;
    }

    private Notification BuildProgressNotification()
    {
        EnsureChannel(ProgressChannelId, "Downloads offline", NotificationImportance.Low);
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, ProgressChannelId) : new Notification.Builder(this);
        builder.SetSmallIcon(Android.Resource.Drawable.StatSysDownload)
            ?.SetContentTitle("PSiptv · Download offline")
            ?.SetContentText("A descarregar conteúdo para o dispositivo")
            ?.SetOngoing(true)
            ?.SetOnlyAlertOnce(true)
            ?.SetContentIntent(OpenDownloads());
        return builder.Build()!;
    }

    private void ShowResultNotification(PSiptv.Core.OfflineDownload download)
    {
        EnsureChannel(CompletedChannelId, "Downloads concluídos", NotificationImportance.Default);
        var completed = download.IsComplete;
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, CompletedChannelId) : new Notification.Builder(this);
        builder.SetSmallIcon(completed ? Android.Resource.Drawable.StatSysDownloadDone : Android.Resource.Drawable.StatNotifyError)
            ?.SetContentTitle(completed ? "Download concluído" : "Falha no download")
            ?.SetContentText(download.Item.Name)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenDownloads());
        try { ((NotificationManager?)GetSystemService(NotificationService))?.Notify(RequestCode(download.Id), builder.Build()); }
        catch (Java.Lang.SecurityException) { }
    }

    private PendingIntent? OpenDownloads() =>
        PSiptv.AndroidNotificationNavigation.Create(this, "downloads", ProgressNotificationId);

    private void EnsureChannel(string id, string name, NotificationImportance importance)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            ((NotificationManager?)GetSystemService(NotificationService))?
                .CreateNotificationChannel(new NotificationChannel(id, name, importance));
    }

    private static int RequestCode(string id) => ProgrammeReminderReceiver.RequestCode(id) ^ 0x24681357;
}

public static partial class OfflineDownloadPlatform
{
    static partial void StartCore(string downloadId)
    {
        var context = Android.App.Application.Context;
        using var intent = new Intent(context, typeof(OfflineDownloadForegroundService));
        intent.PutExtra("id", downloadId);
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
        else context.StartService(intent);
    }
}
