using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using PSiptv.Services;

namespace PSiptv;

// Keep the RFCOMM listener available while minimized. No discovery/polling loop.
[Service(Name = "com.PS.PSiptv.BluetoothSyncForegroundService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class AndroidBluetoothSyncForegroundService : Service
{
    private const string ChannelId = "psiptv.bluetooth.sync";
    private const int NotificationId = 0x5059;
    private const string DisableAction = "psiptv.sync.DISABLE";
    private static volatile bool running;
    internal static bool Running => running;

    internal static void EnsureStarted()
    {
        if (running) return;
        var context = Android.App.Application.Context;
        try { context.StartForegroundService(new Intent(context, typeof(AndroidBluetoothSyncForegroundService))); }
        catch (Java.Lang.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            NearbySyncService.SetStatus("O Android não permitiu ativar a sincronização em segundo plano. Abra a aplicação e confirme a permissão Bluetooth.");
        }
    }

    internal static void Stop()
    {
        Android.App.Application.Context.StopService(new Intent(Android.App.Application.Context,
            typeof(AndroidBluetoothSyncForegroundService)));
    }

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(ChannelId,
            "Sincronização Bluetooth", NotificationImportance.Low));
        var launch = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var open = launch is null ? null : PendingIntent.GetActivity(this, 0, launch,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var disable = PendingIntent.GetService(this, 0,
            new Intent(this, typeof(AndroidBluetoothSyncForegroundService)).SetAction(DisableAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        using var icon = Android.Graphics.Drawables.Icon.CreateWithResource(this, Resource.Drawable.ic_auto_attribution);
        using var actionBuilder = new Notification.Action.Builder(icon, "Desativar", disable);
        using var builder = new Notification.Builder(this, ChannelId);
        builder.SetSmallIcon(Resource.Drawable.ic_auto_attribution);
        builder.SetContentTitle("PSiptv · Sincronização Bluetooth");
        builder.SetContentText("Disponível para sincronizar favoritos com os seus equipamentos.");
        builder.SetContentIntent(open);
        builder.SetOngoing(true);
        builder.SetOnlyAlertOnce(true);
        builder.SetShowWhen(false);
        builder.AddAction(actionBuilder.Build());
        StartForeground(NotificationId, builder.Build()!);
        if (intent?.Action == DisableAction)
        {
            _ = NearbySyncService.SetEnabledAsync(false);
            StopSelf(startId);
        }
        else if (!NearbySyncService.Enabled || AppServices.ActiveAccount is null)
            StopSelf(startId);
        else
        {
            running = true;
            AndroidBluetoothSync.Start();
        }
        // An in-memory unlocked profile cannot be recreated after process death.
        return StartCommandResult.NotSticky;
    }

    public override void OnTaskRemoved(Intent? rootIntent)
    {
        StopSelf();
        base.OnTaskRemoved(rootIntent);
    }

    public override void OnDestroy()
    {
        running = false;
        AndroidBluetoothSync.Stop(stopService: false);
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }

    internal static IDisposable KeepTransferAwake() => new TransferWakeLock();

    private sealed class TransferWakeLock : IDisposable
    {
        private readonly PowerManager.WakeLock? wakeLock;
        internal TransferWakeLock()
        {
            var power = Android.App.Application.Context.GetSystemService(PowerService) as PowerManager;
            wakeLock = power?.NewWakeLock(WakeLockFlags.Partial, "PSiptv:BluetoothSyncTransfer");
            // Bound to the transport's five-minute maximum plus connection time.
            wakeLock?.Acquire(360_000);
        }
        public void Dispose()
        {
            if (wakeLock?.IsHeld == true) wakeLock.Release();
            wakeLock?.Dispose();
        }
    }
}
