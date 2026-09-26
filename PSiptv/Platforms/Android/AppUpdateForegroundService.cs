using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using PSiptv.Core;

namespace PSiptv.Services;

public static class AppUpdateBackgroundDownload
{
    private const string ReadyTagKey = "github-releases.ready-tag";
    private const string ReadyPathKey = "github-releases.ready-path";
    private static readonly object gate = new();
    private static string? activeTag;
    private static AndroidAppRelease? activeRelease;
    private static string? failedTag;
    private static AppUpdateDownloadProgress currentProgress;
    private static string? error;

    public static event Action? Changed;
    public static AndroidAppRelease? ActiveRelease
    {
        get { lock (gate) return activeRelease; }
    }
    public static string? ReadyPath
    {
        get
        {
            if (!ReleaseVersion.TryParse(Preferences.Default.Get(ReadyTagKey, ""), out var available) ||
                !ReleaseVersion.TryParse(AppInfo.Current.VersionString, out var installed) ||
                available.CompareTo(installed) <= 0) return null;
            var path = Preferences.Default.Get(ReadyPathKey, "");
            return File.Exists(path) ? path : null;
        }
    }

    public static (bool Active, AppUpdateDownloadProgress Progress, string? ReadyPath, string? Error) State(string tag)
    {
        lock (gate)
        {
            var path = Preferences.Default.Get(ReadyTagKey, "") == tag ? ReadyPath : null;
            return (activeTag == tag, currentProgress, path,
                activeTag == tag || failedTag != tag ? null : error);
        }
    }

    public static void Start(AndroidAppRelease release)
    {
        lock (gate)
        {
            if (activeTag == release.Tag) return;
            if (activeTag is not null)
                throw new InvalidOperationException("Já está a decorrer o download de outra atualização.");
            activeTag = release.Tag;
            activeRelease = release;
            currentProgress = new(0, release.AssetSize);
            error = null;
            failedTag = null;
        }
        try
        {
            var context = Android.App.Application.Context;
            using var intent = new Intent(context, typeof(AppUpdateForegroundService));
            intent.PutExtra("release", JsonSerializer.Serialize(release));
            if (OperatingSystem.IsAndroidVersionAtLeast(26)) context.StartForegroundService(intent);
            else context.StartService(intent);
            Changed?.Invoke();
        }
        catch
        {
            lock (gate)
            {
                activeTag = null;
                activeRelease = null;
            }
            throw;
        }
    }

    internal static void Report(AppUpdateDownloadProgress progress)
    {
        lock (gate) currentProgress = progress;
        Changed?.Invoke();
    }

    internal static void Running(AndroidAppRelease release)
    {
        lock (gate)
        {
            activeTag = release.Tag;
            activeRelease = release;
            currentProgress = new(0, release.AssetSize);
            error = null;
            failedTag = null;
        }
        Changed?.Invoke();
    }

    internal static void Finish(AndroidAppRelease release, string? path, string? failure)
    {
        lock (gate)
        {
            if (path is not null)
            {
                Preferences.Default.Set(ReadyTagKey, release.Tag);
                Preferences.Default.Set(ReadyPathKey, path);
            }
            activeTag = null;
            activeRelease = null;
            error = failure;
            failedTag = failure is null ? null : release.Tag;
        }
        Changed?.Invoke();
    }
}

[Service(Name = "com.PS.PSiptv.AppUpdateForegroundService", Enabled = true,
    Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class AppUpdateForegroundService : Service
{
    private const string ProgressChannel = "app-update-progress";
    private const string ResultChannel = "app-update-result";
    private const int NotificationId = 48231;
    private bool running;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (running) return StartCommandResult.RedeliverIntent;
        var release = JsonSerializer.Deserialize<AndroidAppRelease>(intent?.GetStringExtra("release") ?? "null");
        if (release is null) { StopSelfResult(startId); return StartCommandResult.NotSticky; }
        running = true;
        AppUpdateBackgroundDownload.Running(release);
        StartForeground(NotificationId, BuildNotification(false, "A descarregar " + release.AssetName, 0));
        _ = Task.Run(async () =>
        {
            PowerManager.WakeLock? wakeLock = null;
            string? path = null;
            string? failure = null;
            try
            {
                wakeLock = ((PowerManager?)GetSystemService(PowerService))?
                    .NewWakeLock(WakeLockFlags.Partial, "PSiptv:AppUpdate");
                wakeLock?.Acquire();
                var progress = new DirectProgress(value =>
                {
                    AppUpdateBackgroundDownload.Report(value);
                    // Avoid rebuilding a system notification for every network buffer.
                    var percent = (int)(value.Fraction * 100);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        try
                        {
                            ((NotificationManager?)GetSystemService(NotificationService))?.Notify(
                                NotificationId, BuildNotification(false, $"A descarregar {release.AssetName} · {percent}%", percent));
                        }
                        catch (Java.Lang.SecurityException) { }
                    }
                });
                path = await AndroidAppUpdateService.DownloadAsync(release, progress, CancellationToken.None);
            }
            catch (Exception ex) { failure = ex.Message; }
            finally
            {
                if (wakeLock?.IsHeld == true) wakeLock.Release();
                wakeLock?.Dispose();
                AppUpdateBackgroundDownload.Finish(release, path, failure);
                StopForeground(StopForegroundFlags.Remove);
                try
                {
                    ((NotificationManager?)GetSystemService(NotificationService))?.Notify(NotificationId,
                        BuildNotification(true, path is null ? "Falha no download da atualização" : "Atualização pronta para instalar", 100));
                }
                catch (Java.Lang.SecurityException) { }
                running = false;
                StopSelfResult(startId);
            }
        });
        return StartCommandResult.RedeliverIntent;
    }

    private int lastPercent = -1;

    private sealed class DirectProgress(Action<AppUpdateDownloadProgress> report) : IProgress<AppUpdateDownloadProgress>
    {
        public void Report(AppUpdateDownloadProgress value) => report(value);
    }

    private Notification BuildNotification(bool complete, string message, int percent)
    {
        var channel = complete ? ResultChannel : ProgressChannel;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
            ((NotificationManager?)GetSystemService(NotificationService))?.CreateNotificationChannel(
                new NotificationChannel(channel, complete ? "Atualizações concluídas" : "Atualização da aplicação",
                    complete ? NotificationImportance.Default : NotificationImportance.Low));
        using var builder = OperatingSystem.IsAndroidVersionAtLeast(26)
            ? new Notification.Builder(this, channel) : new Notification.Builder(this);
        builder.SetSmallIcon(complete ? Android.Resource.Drawable.StatSysDownloadDone : Android.Resource.Drawable.StatSysDownload)
            ?.SetContentTitle("PSiptv · Atualização")
            ?.SetContentText(message)
            ?.SetOngoing(!complete)
            ?.SetAutoCancel(complete)
            ?.SetOnlyAlertOnce(!complete)
            ?.SetContentIntent(PSiptv.AndroidNotificationNavigation.Create(this, "updates", NotificationId));
        if (!complete) builder.SetProgress(100, percent, percent == 0);
        return builder.Build()!;
    }
}
