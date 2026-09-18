using Android.App;
using Android.Content;
using PSiptv.Core;

namespace PSiptv.Services;

[BroadcastReceiver(Name = "com.PS.PSiptv.ProgrammeReminderReceiver", Enabled = true, Exported = false)]
public sealed class ProgrammeReminderReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null) return;
        var id = intent.GetStringExtra("id") ?? "";
        var accountId = intent.GetStringExtra("account") ?? "";
        var profileId = intent.GetStringExtra("profile") ?? "";
        var title = intent.GetStringExtra("title") ?? "Programa";
        var channel = intent.GetStringExtra("channel") ?? "PSiptv";
        ShowNotification(context, id, title, channel);
        if (id.Length > 0 && accountId.Length > 0 && profileId.Length > 0)
            _ = ReminderService.MarkNotifiedAsync(accountId, profileId, id);
    }

    private static void ShowNotification(Context context, string id, string title, string channel)
    {
        const string channelId = "programme-reminders";
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            manager.CreateNotificationChannel(new NotificationChannel(channelId,
                "Lembretes de programas", NotificationImportance.High));

        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        PendingIntent? contentIntent = null;
        if (open is not null)
        {
            open.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
            contentIntent = PendingIntent.GetActivity(context, RequestCode(id), open,
                PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        }
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(context, channelId)
            : new Notification.Builder(context);
        builder.SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            ?.SetContentTitle(title)
            ?.SetContentText($"Começa em breve em {channel}")
            ?.SetAutoCancel(true)
            ?.SetContentIntent(contentIntent);
        var notification = builder.Build();
        if (notification is not null) manager.Notify(RequestCode(id), notification);
    }

    internal static int RequestCode(string id)
    {
        if (id.Length >= 8 && uint.TryParse(id[..8], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value)) return unchecked((int)value);
        return id.GetHashCode(StringComparison.Ordinal);
    }
}

[BroadcastReceiver(Name = "com.PS.PSiptv.ReminderBootReceiver", Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class ReminderBootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != Intent.ActionBootCompleted) return;
        var pending = GoAsync();
        _ = Task.Run(async () =>
        {
            try { await ReminderService.RescheduleAllAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Reminder reschedule failed: {ex.GetType().Name}"); }
            finally { pending?.Finish(); }
        });
    }
}

public static partial class ReminderPlatformScheduler
{
    static partial void ScheduleCore(ProgrammeReminder reminder, DateTimeOffset triggerAt)
    {
        var context = Android.App.Application.Context;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        if (alarm is null) return;
        alarm.SetAndAllowWhileIdle(AlarmType.RtcWakeup, triggerAt.ToUnixTimeMilliseconds(),
            PendingIntentFor(context, reminder, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
    }

    static partial void CancelCore(string reminderId)
    {
        var context = Android.App.Application.Context;
        var alarm = (AlarmManager?)context.GetSystemService(Context.AlarmService);
        var intent = new Intent(context, typeof(ProgrammeReminderReceiver));
        var pending = PendingIntent.GetBroadcast(context, ProgrammeReminderReceiver.RequestCode(reminderId), intent,
            PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
        if (pending is not null) { alarm?.Cancel(pending); pending.Cancel(); pending.Dispose(); }
        intent.Dispose();
    }

    private static PendingIntent PendingIntentFor(Context context, ProgrammeReminder reminder, PendingIntentFlags flags)
    {
        var intent = new Intent(context, typeof(ProgrammeReminderReceiver));
        intent.PutExtra("id", reminder.Id);
        intent.PutExtra("account", reminder.AccountId);
        intent.PutExtra("profile", reminder.ProfileId);
        intent.PutExtra("title", reminder.ProgrammeTitle);
        intent.PutExtra("channel", reminder.ChannelName);
        return PendingIntent.GetBroadcast(context, ProgrammeReminderReceiver.RequestCode(reminder.Id), intent, flags)!;
    }
}
