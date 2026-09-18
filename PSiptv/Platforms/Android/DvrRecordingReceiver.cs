using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using PSiptv.Core;

namespace PSiptv.Services;

[BroadcastReceiver(Name = "com.PS.PSiptv.DvrRecordingReceiver", Enabled = true, Exported = false)]
public sealed class DvrRecordingReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        using var service = new Intent(context, typeof(DvrRecordingForegroundService));
        service.PutExtra("id", intent.GetStringExtra("id") ?? "");
        if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(service);
        else context.StartService(service);
    }

    internal static int RequestCode(string id) => ProgrammeReminderReceiver.RequestCode(id) ^ int.MinValue;
}

[Service(Name = "com.PS.PSiptv.DvrRecordingForegroundService", Enabled = true,
    Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class DvrRecordingForegroundService : Service
{
    private const int NotificationId = 48130;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        StartForeground(NotificationId, BuildNotification());
        var id = intent?.GetStringExtra("id") ?? "";
        _ = Task.Run(async () =>
        {
            PowerManager.WakeLock? wakeLock = null;
            try
            {
                wakeLock = ((PowerManager?)GetSystemService(PowerService))?
                    .NewWakeLock(WakeLockFlags.Partial, "PSiptv:DvrRecording");
                wakeLock?.Acquire();
                if (id.Length > 0) await DvrService.StartScheduledAsync(id);
                else await DvrService.RunDueAsync();
                if (id.Length > 0 && await DvrService.FindByIdAsync(id) is { } result &&
                    !result.Error.Equals("Gravação interrompida pelo utilizador.", StringComparison.Ordinal))
                    ShowResultNotification(result);
            }
            finally
            {
                if (wakeLock?.IsHeld == true) wakeLock.Release();
                wakeLock?.Dispose();
                if (DvrService.ActiveCount == 0) StopSelf();
            }
        });
        return StartCommandResult.RedeliverIntent;
    }

    private Notification BuildNotification()
    {
        const string channelId = "dvr-recordings";
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager?.CreateNotificationChannel(new NotificationChannel(channelId, "Gravações DVR", NotificationImportance.Low));
        var contentIntent = OpenRecordings();
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, channelId) : new Notification.Builder(this);
        builder.SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
            ?.SetContentTitle("PSiptv · Gravação DVR")
            ?.SetContentText("A guardar a transmissão no dispositivo")
            ?.SetOngoing(true)?.SetContentIntent(contentIntent);
        return builder.Build()!;
    }

    private void ShowResultNotification(DvrRecording recording)
    {
        const string channelId = "dvr-recordings-completed";
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager?.CreateNotificationChannel(new NotificationChannel(channelId,
                "Gravações concluídas", NotificationImportance.Default));
        var completed = recording.IsCompleted;
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, channelId) : new Notification.Builder(this);
        builder.SetSmallIcon(completed ? Android.Resource.Drawable.StatSysDownloadDone : Android.Resource.Drawable.StatNotifyError)
            ?.SetContentTitle(completed ? "Gravação concluída" : "Falha na gravação")
            ?.SetContentText(recording.ProgrammeTitle)
            ?.SetAutoCancel(true)
            ?.SetContentIntent(OpenRecordings());
        try { manager?.Notify(ProgrammeReminderReceiver.RequestCode(recording.Id) ^ 0x13572468, builder.Build()); }
        catch (Java.Lang.SecurityException) { }
    }

    private PendingIntent? OpenRecordings() =>
        PSiptv.AndroidNotificationNavigation.Create(this, "recordings", NotificationId);
}

[BroadcastReceiver(Name = "com.PS.PSiptv.DvrBootReceiver", Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class DvrBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try { await DvrService.RescheduleAllAsync(); }
            finally { pending?.Finish(); }
        });
    }
}

public static partial class DvrPlatformScheduler
{
    static partial void ScheduleCore(DvrRecording recording, DateTimeOffset triggerAt)
    {
        var context = Android.App.Application.Context;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarm is null) return;
        alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt.ToUnixTimeMilliseconds(),
            PendingIntentFor(context, recording.Id, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
    }

    static partial void CancelCore(string recordingId)
    {
        var context = Android.App.Application.Context;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        using var intent = new Intent(context, typeof(DvrRecordingReceiver));
        var pending = PendingIntent.GetBroadcast(context, DvrRecordingReceiver.RequestCode(recordingId), intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is null) return;
        alarm?.Cancel(pending);
        pending.Cancel();
        pending.Dispose();
    }

    private static PendingIntent PendingIntentFor(Context context, string id, PendingIntentFlags flags)
    {
        using var intent = new Intent(context, typeof(DvrRecordingReceiver));
        intent.PutExtra("id", id);
        return PendingIntent.GetBroadcast(context, DvrRecordingReceiver.RequestCode(id), intent, flags)!;
    }
}
